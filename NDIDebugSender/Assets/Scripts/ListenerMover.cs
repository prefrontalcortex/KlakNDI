using System.Collections;
using Klak.Ndi.Audio;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

public class ListenerMover : MonoBehaviour
{
    public int channelIndex;
    [SerializeField] private TMP_Text channelText;

    public bool snapPosition = false;
    private Vector3 lastPosition;
    
    public UnityEvent<Vector3> onPositionChanged;
    public UnityEvent<bool> onPointerIsOver;
    public UnityEvent<bool> onIsSelectedChanged;
    private Drag[] _drags;
    
    private void Awake()
    {
        _drags = GetComponentsInChildren<Drag>();
        foreach (var d in _drags)
            d.onMouseIsOverChanged.AddListener(OnMouseOverChanged);
    }

    public void IsSelected(bool selected)
    {
        onIsSelectedChanged.Invoke(selected);
    }

    public void EnableHandle(bool active)
    {
        foreach (var d in _drags)
        {
            d.gameObject.SetActive(active);
        }
    }
    
    private void OnEnable()
    {
        if (channelText)
            channelText.text = channelIndex.ToString();
        onIsSelectedChanged.Invoke(false);
    }

    public void UpdatePosition(Vector3 position)
    {
        transform.position = position;
        lastPosition = position;
    }
    
    private void Update()
    {
        var snappedPosition = transform.position;
        if (this.snapPosition)
        {
            float snapThreshold = 0.5f;
            
            if (snappedPosition.x % snapThreshold > snapThreshold / 2)
                snappedPosition.x += snapThreshold - snappedPosition.x % snapThreshold;
            else
                snappedPosition.x -= snappedPosition.x % snapThreshold;
            
            if (snappedPosition.y % snapThreshold > snapThreshold / 2)
                snappedPosition.y += snapThreshold - snappedPosition.y % snapThreshold;
            else
                snappedPosition.y -= snappedPosition.y % snapThreshold;
            
            if (snappedPosition.z % snapThreshold > snapThreshold / 2)
                snappedPosition.z += snapThreshold - snappedPosition.z % snapThreshold;
            else
                snappedPosition.z -= snappedPosition.z % snapThreshold;
        }
        bool changed = lastPosition != snappedPosition;
        lastPosition = snappedPosition;
        VirtualAudio.UpdateListenerPosition(channelIndex, snappedPosition);
        if (changed)
            onPositionChanged.Invoke(snappedPosition);
    }

    IEnumerator WaitForPointerExit()
    {
        yield return new WaitForSeconds(0.4f);
        onPointerIsOver.Invoke(false);
    }

    private void OnMouseOverChanged(bool isOver)
    {
        StopAllCoroutines();
        if (isOver)
        {
            onPointerIsOver.Invoke(isOver);
        }
        else
        {
            StartCoroutine(WaitForPointerExit());
        }
    }
}
