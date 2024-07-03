using UnityEngine;

public class CameraController : MonoBehaviour
{
    private void Update()
    {
        // Abort when cursor is over UI
        if (UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
            return;
        
        if (Input.GetMouseButton(2))
        {
            var x = Input.GetAxis("Mouse X");
            var y = Input.GetAxis("Mouse Y");
            var rotation = Quaternion.Euler(-y, x, 0);
            var position = transform.position;
            transform.position = rotation * position;
            transform.LookAt(Vector3.zero);
        }
        
        // Zoom in and out
        var scroll = Input.mouseScrollDelta;
        if (scroll.y != 0)
        {
            if (scroll.y > 0 && transform.position.magnitude < 1)
                return;
            transform.position += transform.forward * scroll.y;
        }
    }
}
