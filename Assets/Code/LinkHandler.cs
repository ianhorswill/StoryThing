using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

[RequireComponent(typeof(TextMeshProUGUI))]
public class LinkHandler : MonoBehaviour, IPointerClickHandler {

    public void OnPointerClick(PointerEventData eventData) {
        var text = GetComponent<TextMeshProUGUI>();
        int linkIndex = TMP_TextUtilities.FindIntersectingLink(text, Input.mousePosition, null);
        if( linkIndex != -1 ) { // was a link clicked?
            TMP_LinkInfo linkInfo = text.textInfo.linkInfo[linkIndex];

            // open the link id as a url, which is the metadata we added in the text field
            Driver.Singleton.OnClick(linkInfo.GetLinkID());
        }
    }
}