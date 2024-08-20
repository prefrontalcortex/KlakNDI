using System;
using System.Collections;
using System.Collections.Generic;
using Klak.Ndi;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

public class RenderTextureSetup : MonoBehaviour
{
    [SerializeField] private NdiSender _ndiSender;
    [SerializeField] private Camera _camera;
    [SerializeField] private TMP_Dropdown _presetDropdown;
    
    [Serializable]
    public struct ResolutionPreset
    {
        public int width;
        public int height;
    }

    [SerializeField] private ResolutionPreset[] preset;
    private RenderTexture _renderTexture;
    
    public UnityEvent OnResolutionChanged = new UnityEvent();
    
    public void SelectPreset(int index)
    {
        if (index < 0 || index >= preset.Length)
        {
            Debug.LogError("Invalid index");
            return;
        }
        
        ChangeResolution(preset[index].width, preset[index].height);
    }
    
    public void ChangeResolution(int width, int height)
    {
        _ndiSender.enabled = false;

        if (_renderTexture != null)
        {
            _renderTexture.Release();
            Destroy(_renderTexture);
        }
        _renderTexture = new RenderTexture(width,height,24,RenderTextureFormat.ARGB32);
        
        _ndiSender.sourceTexture = _renderTexture;
        _camera.targetTexture = _renderTexture;
        _ndiSender.enabled = true;
        
        OnResolutionChanged.Invoke();
    }

    private void CreatePresetDropDown()
    {
        _presetDropdown.ClearOptions();
        List<string> options = new List<string>();
        foreach (var resolutionPreset in preset)
        {
            options.Add($"{resolutionPreset.width}x{resolutionPreset.height}");
        }
        _presetDropdown.AddOptions(options);
        _presetDropdown.SetValueWithoutNotify(0);
    }

    private void Awake()
    {
        CreatePresetDropDown();
        _presetDropdown.onValueChanged.AddListener(SelectPreset);
    }
}
