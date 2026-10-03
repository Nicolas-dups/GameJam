using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  POSTE DE POLICE : fait apparaître une voiture de police qui patrouille
//  et poursuit les infractionnistes (voir PoliceCar).
//  Le prefab de la voiture est `policePrefab` du GameManager.
// =====================================================================
public class PoliceStation : RoadElement
{
    public override void OnRunStart(int seed) => GameManager.Instance.SpawnCar(Node, seed, true);
}
