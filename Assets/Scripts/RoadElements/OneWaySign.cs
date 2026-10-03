using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  SENS UNIQUE : interdit de rouler contre `Facing` sur cette tuile
//  (R en phase de placement inverse le sens)
// =====================================================================
public class OneWaySign : RoadElement
{
    protected override void OnPlaced() => RoadGraph.Instance.SetOneWay(Node, Facing, true);
    protected override void OnRemoved() => RoadGraph.Instance.SetOneWay(Node, Facing, false);
}
