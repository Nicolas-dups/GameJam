using UnityEngine;

public class BorderInteractible : MonoBehaviour, IPointerInteractible
{
    public new Transform transform => GetComponent<Transform>();
}
