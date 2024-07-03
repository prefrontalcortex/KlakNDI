using UnityEngine;
using UnityEngine.EventSystems;

public class DropTargetUI : MonoBehaviour, IDropHandler
{
    public ConfigEditor configEditor;
    
    public RectTransform previewRect;

    private int _dropPositionIndex;
    
    private void Awake()
    {
        previewRect.gameObject.SetActive(false);
    }
    
    public void OnDragPreview(ListenerUI dragObject)
    {
        previewRect.gameObject.SetActive(true);
        var listeners =GetComponentsInChildren<ListenerUI>();
        var pos = dragObject.GetComponent<RectTransform>().anchoredPosition.y;
        ListenerUI closest = null;
        var closestDistance = float.MaxValue;
        int index = -1;
        foreach (var listener in listeners)
        {
            index++;
            
            if (listener == dragObject)
                continue;
            
            var distance = Mathf.Abs(listener.GetComponent<RectTransform>().anchoredPosition.y - pos);
            if (!closest || distance < closestDistance)
            {
                _dropPositionIndex = index;
                closest = listener;
                closestDistance = distance;
            }
        }

        if (closest)
        {
            var closetRect = closest.GetComponent<RectTransform>();
            var closetY = closetRect.anchoredPosition.y;
            
            if (pos > closetY)
            {
                previewRect.anchoredPosition = closest.GetComponent<RectTransform>().anchoredPosition + new Vector2(0, closetRect.sizeDelta.y / 2f);
            }
            else
            {
                _dropPositionIndex++;
                previewRect.anchoredPosition = closest.GetComponent<RectTransform>().anchoredPosition - new Vector2(0, closetRect.sizeDelta.y / 2f);
            }
        }
        previewRect.transform.SetAsLastSibling();
    }

    public void HidePreview()
    {
        previewRect.gameObject.SetActive(false);
    }

    public void OnDrop(PointerEventData eventData)
    {
        var draggable = eventData.pointerDrag.GetComponent<ListenerUI>();
        if (draggable)
        {
            configEditor.ReorderListener(draggable, _dropPositionIndex);        
        }
    }
}