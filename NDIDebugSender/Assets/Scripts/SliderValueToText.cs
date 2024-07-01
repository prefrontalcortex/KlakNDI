using TMPro;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Slider))]
public class SliderValueToText : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI _text;

    private Slider _slider;

    private void Awake()
    {
        _slider = GetComponent<Slider>();
        _slider.onValueChanged.AddListener(UpdateText);
        UpdateText(_slider.value);
    }

    private void OnEnable()
    {
        UpdateText(_slider.value);
    }

    private void UpdateText(float value)
    {
        _text.text = value.ToString("F2");
    }
}
