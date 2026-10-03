using UnityEngine;
using System;

public abstract class Item : ScriptableObject
{
    public string itemName;
    public GameObject prefab;
    public GameObject itemPreviewPrefab;
    public Action MapCsq;
    public abstract Type interactibleType { get; }
}
