using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Ping au-dessus d'un objet (voiture...), piloté par code.
/// Plusieurs "raisons" peuvent coexister (clé unique par raison) : le sprite de plus haute priorité est affiché.
/// Durée en temps de jeu (suit Time.timeScale) ; durée < 0 = jusqu'à Hide(key).
/// </summary>
public class StatusPing : MonoBehaviour
{
    public Vector3 offset = new Vector3(0f, 3.5f, 0f);
    public float scale = 1f;
    public float bobAmplitude = 0.25f;
    public float bobSpeed = 3f;
    public int sortingOrder = 100;

    class Entry { public string key, sprite; public float end; public int priority; }

    readonly List<Entry> entries = new List<Entry>();
    GameObject obj;
    SpriteRenderer sr;
    string shownSprite;

    static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();
    static readonly List<StatusPing> all = new List<StatusPing>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { all.Clear(); cache.Clear(); }

    /// <summary>Retire tous les pings de la scène (accident, rembobinage...).</summary>
    public static void ClearAll()
    {
        foreach (var p in all) if (p != null) p.HideAll();
    }

    /// <summary>Ping sur l'objet donné (le composant est ajouté si absent).</summary>
    public static StatusPing On(GameObject go)
    {
        var p = go.GetComponent<StatusPing>();
        return p != null ? p : go.AddComponent<StatusPing>();
    }

    void OnEnable() => all.Add(this);

    void OnDisable()
    {
        all.Remove(this);
        entries.Clear();
        if (obj != null) Destroy(obj);
        obj = null; sr = null; shownSprite = null;
    }

    // ---------- API ----------
    /// <summary>
    /// Affiche `spriteName` (dans Resources/) sous la raison `key`.
    /// Rappeler Show avec la même clé prolonge simplement la durée (pratique pour "tant que...").
    /// </summary>
    public void Show(string key, string spriteName, float duration = -1f, int priority = 0)
    {
        Entry e = null;
        for (int i = 0; i < entries.Count; i++) if (entries[i].key == key) { e = entries[i]; break; }
        if (e == null) { e = new Entry { key = key }; entries.Add(e); }
        e.sprite = spriteName;
        e.priority = priority;
        e.end = duration < 0f ? float.MaxValue : Time.time + duration;
    }

    public void Hide(string key)
    {
        for (int i = entries.Count - 1; i >= 0; i--) if (entries[i].key == key) entries.RemoveAt(i);
    }

    public void HideAll() => entries.Clear();

    // ---------- Affichage ----------
    void LateUpdate()
    {
        Entry best = null;
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            if (Time.time >= entries[i].end) { entries.RemoveAt(i); continue; }
            if (best == null || entries[i].priority > best.priority) best = entries[i];
        }

        if (best == null) { if (obj != null && obj.activeSelf) obj.SetActive(false); return; }

        if (obj == null)
        {
            obj = new GameObject("StatusPing");
            sr = obj.AddComponent<SpriteRenderer>();
            sr.sortingOrder = sortingOrder;
        }

        if (shownSprite != best.sprite)
        {
            shownSprite = best.sprite;
            sr.sprite = Load(best.sprite);
        }
        obj.SetActive(sr.sprite != null);
        if (sr.sprite == null) return;

        float bob = Mathf.Sin(Time.unscaledTime * bobSpeed) * bobAmplitude;
        obj.transform.position = transform.position + offset + Vector3.up * bob;
        obj.transform.localScale = Vector3.one * scale;

        var cam = Camera.main;
        if (cam != null)
        {
            Vector3 toCam = cam.transform.position - obj.transform.position;
            toCam.y = 0f;
            if (toCam.sqrMagnitude > 0f) obj.transform.rotation = Quaternion.LookRotation(toCam, Vector3.up);
        }
    }

    static Sprite Load(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (cache.TryGetValue(name, out var s)) return s;
        s = Resources.Load<Sprite>(name);
        if (s == null) Debug.LogError($"StatusPing : sprite '{name}' introuvable dans Resources.");
        cache[name] = s;
        return s;
    }
}