using UnityEngine;
using TMPro;
using UnityEngine.EventSystems;

public class HelpText : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{

    public TMP_Text helpText;

    void Start()
    {
        if (helpText != null)
        {
            helpText.gameObject.SetActive(false);
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (helpText != null)
        {
            helpText.gameObject.SetActive(true);
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (helpText != null)
        {
            helpText.gameObject.SetActive(false);
        }
    }

    
}
