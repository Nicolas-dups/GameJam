using UnityEngine;
using System;

[CreateAssetMenu(fileName = "New Barrier", menuName = "Items/Barrier")]
public class Barrier : Item
{
    public override Type interactibleType => typeof(BorderInteractible);

}
