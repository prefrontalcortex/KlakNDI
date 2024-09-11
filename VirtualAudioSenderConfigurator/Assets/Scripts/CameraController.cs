using UnityEngine;

public class CameraController : MonoBehaviour
{
    [SerializeField] private Transform _xRotationTransform;
    [SerializeField] private Transform _yRotationTransform;
    [SerializeField] private Transform _zoomTransform;
    private Vector3 _startPosition;
    private Quaternion _startRotation;
    
    private void Awake()
    {
        _startRotation = transform.rotation;
        _startPosition = transform.position;
    }
    
    public void TopDownView()
    {
        _xRotationTransform.localRotation = Quaternion.identity;
        _yRotationTransform.localRotation = Quaternion.identity;
    }
    
    private void Update()
    {
        // Abort when cursor is over UI
        if (UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
            return;
        
        // Zoom in and out
        var scroll = Input.mouseScrollDelta;
        if (scroll.y != 0)
        {
            _zoomTransform.localPosition += Vector3.up * scroll.y;
            if (_zoomTransform.localPosition.y < 4)
                _zoomTransform.localPosition = Vector3.up * 4f;
            if (_zoomTransform.localPosition.y > 40)
                _zoomTransform.localPosition = Vector3.up * 40f;
        }
        if (Input.GetMouseButton(2))
        {
            var oldRotx = _xRotationTransform.localRotation;
            
            var x = Input.GetAxis("Mouse X") * 2f;
            var y = Input.GetAxis("Mouse Y") * 2f;
            _yRotationTransform.localRotation *= Quaternion.Euler(0, x, 0);
            
            _xRotationTransform.localRotation *= Quaternion.Euler(-y, 0, 0);
            if (!(_xRotationTransform.localRotation.eulerAngles.x >= 280 && _xRotationTransform.localRotation.eulerAngles.x <= 360))
                _xRotationTransform.localRotation = oldRotx;
        }    
    }
}
