using UnityEngine;
using System;

[CreateAssetMenu(fileName = "New Stop Sign", menuName = "Items/Stop Sign")]
public class StopSign : Item
{
    public override Type interactibleType => typeof(CornerInteractible);

}
