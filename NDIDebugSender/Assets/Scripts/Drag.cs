using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;

[RequireComponent(typeof(Collider))]
public class Drag : MonoBehaviour
{
    public enum AxisOption {CameraForwad, PlanarXZ, PlanarYZ, PlanarXY , AxisX, AxisY, AxisZ}
    public Transform target;
    public AxisOption axis = AxisOption.CameraForwad;
    private Plane _plane;
    private Vector3 _offset;
    
    [FormerlySerializedAs("OnMouseIsOverChanged")] public UnityEvent<bool> onMouseIsOverChanged;

    private Camera mainCamera;

    private Vector3 _initialScale;
    private void Start()
    {
        mainCamera = Camera.main;
        _initialScale = transform.localScale;
    }

    private void OnMouseEnter()
    {
        transform.localScale = _initialScale * 1.5f;
        onMouseIsOverChanged.Invoke(true);
    }
    
    private void OnMouseExit()
    {
        transform.localScale = _initialScale;
        onMouseIsOverChanged.Invoke(false);
    }

    private void OnMouseDown()
    {
        _plane = axis switch
        {
            AxisOption.CameraForwad => new Plane(mainCamera.transform.forward, target.position),
            AxisOption.PlanarXZ => new Plane(Vector3.up, target.position),
            AxisOption.PlanarXY => new Plane( Vector3.forward, target.position),
            AxisOption.PlanarYZ => new Plane(Vector3.right, target.position),
            AxisOption.AxisX => new Plane(Vector3.up, target.position),
            AxisOption.AxisY => new Plane(mainCamera.transform.rotation * Vector3.forward, target.position),
            AxisOption.AxisZ => new Plane(Vector3.up, target.position),
     
            _ => throw new ArgumentOutOfRangeException()
        };
        Ray camRay = mainCamera.ScreenPointToRay(Input.mousePosition);

        _plane.Raycast(camRay, out var planeDistance);
        var p = camRay.GetPoint(planeDistance);
        switch (axis)
        {
            case AxisOption.AxisX:
                p = Vector3.Scale(p, new Vector3(1f, 0, 0));
                break;
            case AxisOption.AxisY:
                p = Vector3.Scale(p, new Vector3(0, 1f, 0));
                break;
            case AxisOption.AxisZ:
                p = Vector3.Scale(p, new Vector3(0, 0, 1f));
                break;
        }
        _offset = target.position - p;
    }
    
    private void OnMouseDrag()
    {
        Ray camRay = mainCamera.ScreenPointToRay(Input.mousePosition);
        _plane.Raycast(camRay, out var planeDistance);
        var p = camRay.GetPoint(planeDistance);
        switch (axis)
        {
            case AxisOption.AxisX:
                p = Vector3.Scale(p, new Vector3(1f, 0, 0));
                break;
            case AxisOption.AxisY:
                p = Vector3.Scale(p, new Vector3(0, 1f, 0));
                break;
            case AxisOption.AxisZ:
                p = Vector3.Scale(p, new Vector3(0, 0, 1f));
                break;
        }
        target.position = p + _offset;
    }
}
