using Klak.Ndi.Audio;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using UnityEngine.EventSystems;

public class ListenerUI : MonoBehaviour, IDragHandler, IBeginDragHandler, IEndDragHandler
{
    public Toggle selected;
    public TMP_InputField xPos;
    public TMP_InputField yPos;
    public TMP_InputField zPos;
    public TextMeshProUGUI channelText;
    public Slider volumeSlider;

    public Image highlightImage;
    public Color highlightColor;
    private Color orgColor;
    
    public int channelIndex;

    private Vector3 _currentPos;
    public Vector3 currentPos { get => _currentPos; set => UpdatePosition(value); }

    public UnityEvent<Vector3> onPositionChanged;

    public void Select()
    {
        selected.SetIsOnWithoutNotify(true);
    }

    public void Unselect()
    {
        selected.SetIsOnWithoutNotify(false);
    }
    
    private void Awake()
    {
        volumeSlider.onValueChanged.AddListener(OnVolumeChanged);
        xPos.onEndEdit.AddListener(OnXPosChanged);
        yPos.onEndEdit.AddListener(OnYPosChanged);
        zPos.onEndEdit.AddListener(OnZPosChanged);

        orgColor = highlightImage.color;
    }

    public void UpdatePosition(Vector3 newPos)
    {
        _currentPos = newPos;
        UpdateView();
    }

    public void UpdateVolume(float newVol)
    {
        volumeSlider.SetValueWithoutNotify(newVol);
    }

    private void OnZPosChanged(string value)
    {
        var newValue = float.Parse(value);
        _currentPos.z = newValue;
        VirtualAudio.UpdateListenerPosition(channelIndex, currentPos);
        onPositionChanged.Invoke(currentPos);
    }

    private void OnYPosChanged(string value)
    {
        var newValue = float.Parse(value);
        _currentPos.y = newValue;
        VirtualAudio.UpdateListenerPosition(channelIndex, currentPos);
        onPositionChanged.Invoke(currentPos);
    }

    private void OnXPosChanged(string value)
    {
        var newValue = float.Parse(value);
        _currentPos.x = newValue;
        VirtualAudio.UpdateListenerPosition(channelIndex, currentPos);
        onPositionChanged.Invoke(currentPos);
    }

    private void OnVolumeChanged(float volume)
    {
        VirtualAudio.SetListenerVolume(channelIndex, volume);
    }

    public void Init(int channelIndex, Vector3 pos)
    {
        selected.SetIsOnWithoutNotify(false);
        this.channelIndex = channelIndex;
        _currentPos = pos;
        highlightImage.color = orgColor;
        UpdateView();
    }

    private void UpdateView()
    {
        xPos.text = currentPos.x.ToString("F1");
        yPos.text = currentPos.y.ToString("F1");
        zPos.text = currentPos.z.ToString("F1");
        channelText.text = (channelIndex + 1).ToString();
        volumeSlider.value = VirtualAudio.GetListenersVolume()[channelIndex];
    }

    private Vector3 _beforeDragOriginalPosition;
    private DropTargetUI _dropTarget;
    private ScrollRect _scrollRect;
    
    public void OnBeginDrag(PointerEventData eventData)
    {
        _beforeDragOriginalPosition = transform.position;
        transform.SetAsLastSibling();
        _scrollRect = GetComponentInParent<ScrollRect>();
        var canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup)
            canvasGroup.alpha = 0.5f;
    }

    public void OnDrag(PointerEventData eventData)
    {
        var newPos = Input.mousePosition;
        transform.position = new Vector3(transform.position.x, newPos.y, transform.position.z);

        if (_scrollRect)
        {
            var scrollOverallRect = _scrollRect.GetComponent<RectTransform>();
            var contentRect = _scrollRect.content;
            var relativePos = scrollOverallRect.InverseTransformPoint(newPos);

            // Auto scroll vertically when mouse is near top or bottom border of scrollOverallRect

            // 3 Items per second
            float itemHeight = GetComponent<RectTransform>().rect.height;
            float scrollVelocity = itemHeight * 4;
            if (relativePos.y > scrollOverallRect.rect.yMax - itemHeight * 3f)
            {
                _scrollRect.velocity = new Vector2(0, -scrollVelocity);
            }
            else if (relativePos.y < scrollOverallRect.rect.yMin + itemHeight * 3f)
            {
                _scrollRect.velocity = new Vector2(0, scrollVelocity);
            }
            else
            {
                _scrollRect.velocity = Vector2.zero;
            }
        }
        
        if (!_dropTarget)
            _dropTarget = GetComponentInParent<DropTargetUI>();

        _dropTarget.OnDragPreview(this);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (!eventData.pointerEnter || !eventData.pointerEnter.GetComponent<DropTargetUI>())
        {
            transform.position = _beforeDragOriginalPosition;
        }
        var canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup)
            canvasGroup.alpha = 1f;

        _dropTarget.HidePreview();
    }

    public void Highlight(bool isOver)
    {
        highlightImage.color = isOver ? highlightColor : orgColor;
    }
}