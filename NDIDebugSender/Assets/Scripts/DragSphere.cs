using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class DragSphere : MonoBehaviour
{
    public Vector3 dragPositions = new Vector3(1f, 0f, 1f);
    
    private Collider _collider;
    private bool _isDragging;
    private Vector3 _offset;
    private Vector3 _startPosition;
    private Vector2 _startMousePosition;
    
    
    private void Awake()
    {
        _collider = GetComponent<Collider>();
    }

    
    private void Update()
    {
        if (_isDragging)
        {
            if (Input.GetMouseButtonUp(0))
            {
                _isDragging = false;
            }
            else
            {
                // Calculate the offset
                Vector3 currentMousePosition = Camera.main.ScreenToWorldPoint(Input.mousePosition);

                //var diff = currentMousePosition - _startMousePosition;
                //_offset = new Vector3(diff.x, diff.y, 0);
                
                
                //transform.position = _startPosition + _offset;
                // Calculate the new position
                //Vector3 newPosition = Camera.main.ScreenToWorldPoint(currentMousePosition);
                //transform.position = newPosition + _offset;
            }
            
        }
        else
        if (Input.GetMouseButtonDown(0))
        {
            
            
            // Raycast from Screen to World and check for collider hit
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            RaycastHit hit;
            if (Physics.Raycast(ray, out hit))
            {
                // If hit, store the hit position
                dragPositions = hit.point;
                if (hit.collider == _collider)
                {
                    _isDragging = true;
                    _startMousePosition = Camera.main.ScreenToWorldPoint(Input.mousePosition);
                    _startPosition = transform.position;
                    _offset = Vector3.zero;
                }
            }
            
        }
        
    }
    
    
}
