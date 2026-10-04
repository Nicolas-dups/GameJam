using UnityEngine;
using TMPro;
using UnityEngine.EventSystems;

public class HelpText : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{

    public GameObject helpText;

    void OnEnable()
    {
        if (helpText != null)
        {
            helpText.SetActive(false);
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (helpText != null)
        {
            helpText.SetActive(true);
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (helpText != null)
        {
            helpText.SetActive(false);
        }
    }

    public void OnDisable()
        {
            if (helpText != null)
            {
                helpText.SetActive(false);
            }
        }

    
}
