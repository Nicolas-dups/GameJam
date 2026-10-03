using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Enregistre position / rotation d'objets pendant la simulation, puis les rejoue à l'envers
/// (en temps réel non mis à l'échelle : fonctionne avec Time.timeScale = 0).
/// </summary>
public class TimeRewinder
{
    struct Sample { public Vector3 pos; public Quaternion rot; }

    class Track
    {
        public Transform tr;
        public bool smoothStart;          // caméra : rejoint la piste en douceur au début du rembobinage
        public Animator[] animators;
        public List<Sample> samples = new List<Sample>(2048);
    }

    readonly List<Track> tracks = new List<Track>();
    float interval = 1f / 60f;            // temps de simulation entre deux échantillons
    int count;                            // nombre d'échantillons (identique pour toutes les pistes)

    public bool CanPlay => count >= 2 && tracks.Count > 0;
    public float RecordedSeconds => Mathf.Max(0, count - 1) * interval;

    public void Clear(float sampleInterval)
    {
        tracks.Clear();
        count = 0;
        interval = Mathf.Max(0.001f, sampleInterval);
    }

    /// <summary>Ajoute un objet à enregistrer (à appeler avant le premier Record).</summary>
    public void Add(Transform tr, bool smoothStart = false)
    {
        if (tr == null) return;
        var track = new Track
        {
            tr = tr,
            smoothStart = smoothStart,
            animators = tr.GetComponentsInChildren<Animator>(true)
        };
        // objet ajouté en cours de simulation : il reste sur place avant son apparition
        var s = Capture(tr);
        for (int i = 0; i < count; i++) track.samples.Add(s);
        tracks.Add(track);
    }

    public void Record()
    {
        foreach (var t in tracks)
        {
            if (t.tr == null)   // objet détruit en cours de route : on garde sa dernière pose
                t.samples.Add(t.samples.Count > 0 ? t.samples[t.samples.Count - 1] : default);
            else
                t.samples.Add(Capture(t.tr));
        }
        count++;
    }

    static Sample Capture(Transform t) => new Sample { pos = t.position, rot = t.rotation };

    /// <summary>Rejoue l'enregistrement à l'envers en `duration` secondes réelles.</summary>
    public IEnumerator Play(float duration, float animatorSpeed, float cameraBlendTime = 0.6f)
    {
        if (!CanPlay) yield break;

        var startPos = new Vector3[tracks.Count];
        var startRot = new Quaternion[tracks.Count];
        for (int k = 0; k < tracks.Count; k++)
        {
            var tr = tracks[k].tr;
            if (tr == null) continue;
            startPos[k] = tr.position;
            startRot[k] = tr.rotation;
            foreach (var a in tracks[k].animators)
            {
                if (a == null) continue;
                a.updateMode = AnimatorUpdateMode.UnscaledTime;   // le jeu est en pause (timeScale = 0)
                a.speed = animatorSpeed;                          // négatif : animation à l'envers
            }
        }

        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(t / duration);
            float eased = u * u * (3f - 2f * u);                  // démarre / finit en douceur
            float blend = Mathf.SmoothStep(0f, 1f, t / cameraBlendTime);
            Apply((1f - eased) * (count - 1), blend, startPos, startRot);
            yield return null;
        }

        Apply(0f, 1f, startPos, startRot);                        // état exact du départ

        foreach (var track in tracks)
            foreach (var a in track.animators)
                if (a != null) a.speed = 0f;                      // fige les animations
    }

    void Apply(float f, float blend, Vector3[] startPos, Quaternion[] startRot)
    {
        int i0 = Mathf.Clamp(Mathf.FloorToInt(f), 0, count - 1);
        int i1 = Mathf.Min(i0 + 1, count - 1);
        float a = f - i0;

        for (int k = 0; k < tracks.Count; k++)
        {
            var track = tracks[k];
            if (track.tr == null) continue;

            Vector3 p = Vector3.Lerp(track.samples[i0].pos, track.samples[i1].pos, a);
            Quaternion r = Quaternion.Slerp(track.samples[i0].rot, track.samples[i1].rot, a);

            if (track.smoothStart && blend < 1f)
            {
                p = Vector3.Lerp(startPos[k], p, blend);
                r = Quaternion.Slerp(startRot[k], r, blend);
            }
            track.tr.SetPositionAndRotation(p, r);
        }
    }
}