using UnityEngine;
using UnityEngine.InputSystem;

public class Shop : MonoBehaviour
{
    public GameObject item0;
    public GameObject item1;
    public GameObject item2;
    public GameObject item3;

    private GameObject current_item;


    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        current_item = null;
    }

    // Update is called once per frame
    void Update()
    {
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            current_item = null;
        }

        PrefabControl();
        //Debug.Log(current_item);
        
    }

    public void ItemSelected(int ItemRef)
    {
        Debug.Log("ItemSelected");
        switch(ItemRef)
            {
                case  0:
                    current_item = Instantiate(item0);
                    break;
                case  1:
                    current_item = Instantiate(item1);
                    break;
                case  2:
                    current_item = Instantiate(item2);
                    break;
                case  3:
                    current_item = Instantiate(item3);
                    break;
                default:
                    print("No item Assigned to this button");
                    break;

            }
    }

   
    private void PrefabControl()
    {
        if (current_item == null)
            return;

        Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            current_item.transform.position = hit.point;
            current_item.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        }
        
    }
    
        
}
