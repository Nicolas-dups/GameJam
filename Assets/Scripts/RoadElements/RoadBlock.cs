using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  ROUTE BARRÉE : la tuile est retirée du graphe, les voitures contournent
//  (prefab conseillé avec centerOnRoad = true)
// =====================================================================
public class RoadBlock : RoadElement
{
    protected override void OnPlaced() => RoadGraph.Instance.SetBlocked(Node, true);
    protected override void OnRemoved() => RoadGraph.Instance.SetBlocked(Node, false);
}
