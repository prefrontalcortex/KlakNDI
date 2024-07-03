using System;
using TMPro;
using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class LineToGround : MonoBehaviour
{
    private LineRenderer _lineRenderer;
    public enum ModeOptions {FromThisToGround, GroundToCenter}
    public ModeOptions mode = ModeOptions.FromThisToGround;

    public TextMeshPro distanceLabel;
    
    private void Awake()
    {
        _lineRenderer = GetComponent<LineRenderer>();
        _lineRenderer.useWorldSpace = true;
    }

    public void SetPosition(Vector3 p1, Vector3 p2)
    {
        _lineRenderer.SetPositions(new []{p1, p2});
        if (distanceLabel)
        {
            var distance = Vector3.Distance(p1, p2);
            distanceLabel.text = $"{distance:0.00}m";
            var labelPos = Vector3.Lerp(p1, p2, 0.5f); 
            
            // Show label on side of the line
            labelPos += Vector3.Cross(p2 - p1, Vector3.up).normalized * 0.2f;
            labelPos.y += 0.1f;
            distanceLabel.transform.SetPositionAndRotation( labelPos, 
                    Quaternion.LookRotation( Camera.main.transform.forward));
        }
    }

    private void LateUpdate()
    {
        var pos = transform.position;
        if (mode == ModeOptions.FromThisToGround)
            SetPosition(pos, new Vector3(pos.x, 0f, pos.z));
        else if (mode == ModeOptions.GroundToCenter)
            SetPosition(Vector3.zero, new Vector3(pos.x, 0f, pos.z));
            
    }
}
