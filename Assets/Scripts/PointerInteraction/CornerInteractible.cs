using UnityEngine;

public class CornerInteractible : MonoBehaviour, IPointerInteractible
{
    public new Transform transform => GetComponent<Transform>();
}
