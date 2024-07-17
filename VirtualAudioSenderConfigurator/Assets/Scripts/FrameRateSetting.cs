using System;
using System.Linq;
using Klak.Ndi;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class FrameRateSetting : MonoBehaviour
{
    [SerializeField] private NdiSender _ndiSender;
    [SerializeField] private TMP_Dropdown _dropdown;
    [SerializeField] private Toggle _setRenderTargetFrameRateToggle;
    private void Awake()
    {
        _dropdown.onValueChanged.AddListener(OnValueChanged);
        _setRenderTargetFrameRateToggle.onValueChanged.AddListener(OnSetRenderTargetFrameRateValueChanged);
        CreateDropdownOptions();
    }

    private void OnSetRenderTargetFrameRateValueChanged(bool isOn)
    {
        _ndiSender.setRenderTargetFrameRate = isOn;
    }

    private void CreateDropdownOptions()
    {
        _dropdown.ClearOptions();
        var list = Enum.GetValues(typeof(FrameRateOptions)).Cast<int>().Select(
                e =>
                {
                    ((FrameRateOptions)e).GetND(out var n, out var d);
                    return ((float)n / (float)d).ToString("F");
                })
            .ToList();
        _dropdown.AddOptions(list);
    }

    private void OnEnable()
    {
        Update();
    }

    private void Update()
    {
        if (_dropdown.value != (int)_ndiSender.frameRate)
            _dropdown.SetValueWithoutNotify((int)_ndiSender.frameRate);
        
        if (_setRenderTargetFrameRateToggle.isOn != _ndiSender.setRenderTargetFrameRate)
            _setRenderTargetFrameRateToggle.SetIsOnWithoutNotify(_ndiSender.setRenderTargetFrameRate);
    }

    private void OnValueChanged(int index)
    {
        _ndiSender.frameRate = (FrameRateOptions)index;
    }
}
