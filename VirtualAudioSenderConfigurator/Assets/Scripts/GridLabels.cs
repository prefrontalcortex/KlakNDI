using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class GridLabels : MonoBehaviour
{
    [SerializeField] private TextMeshPro _labelTemplate;

    [SerializeField] private int maxMeters = 20;
    private List<TextMeshPro> _labels = new List<TextMeshPro>(50);

    private void CreateLabels()
    {
        float x = 0;
        float y = 0;
        for (x = -maxMeters; x < maxMeters; x++) 
        {
            if (x == 0)
                continue;
            var label = Instantiate(_labelTemplate, transform);
            label.transform.localPosition = new Vector3(x, 0.02f, y);
            label.text = $"{x}m";
            label.gameObject.SetActive(true);
            _labels.Add(label);
        }

        x = 0f;
        for (y = -maxMeters; y < maxMeters; y++) 
        {
            if (y == 0)
                continue;
            var label = Instantiate(_labelTemplate, transform);
            label.transform.localPosition = new Vector3(x, 0.02f, y);
            label.gameObject.SetActive(true);
            label.text = $"{y}m";
            _labels.Add(label);
        }
    }

    private void Awake()
    {
        _labelTemplate.gameObject.SetActive(false);
        CreateLabels();
    }
}
