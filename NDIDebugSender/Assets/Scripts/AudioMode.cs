using System.Collections.Generic;
using Klak.Ndi;
using Klak.Ndi.Audio;
using TMPro;
using UnityEngine;

public class AudioMode : MonoBehaviour
{
    [SerializeField] private NdiSender _ndiSender;
    [SerializeField] private TMP_Dropdown _audioModeDropdown;

    [SerializeField] private SpeakerConfig[] _customSpeakerConfigs;

    private class AudioDropDownOption : TMP_Dropdown.OptionData
    {
        public NdiSender.AudioMode mode;
        public SpeakerConfig config;
        public bool onlyText = false;
        
        public AudioDropDownOption(NdiSender.AudioMode mode)
        {
            this.mode = mode;
            text = mode.ToString();
        }
        
        public AudioDropDownOption(SpeakerConfig config)
        {
            this.mode = NdiSender.AudioMode.CustomSpeakerConfig;
            this.config = config;
            text = config.name;
        }

        public AudioDropDownOption(string text)
        {
            onlyText = true;
            this.text = text;
        }
    }
    
    private List<TMP_Dropdown.OptionData> _options = new List<TMP_Dropdown.OptionData>();
    private int _lastModeIndex = 0;
    
    private void UpdateDropdown()
    {
        
        foreach (var mode in (NdiSender.AudioMode[]) System.Enum.GetValues(typeof(NdiSender.AudioMode)))
        {
            if (mode != NdiSender.AudioMode.CustomSpeakerConfig)
                _options.Add(new AudioDropDownOption(mode));
        }
        
        _options.Add(new AudioDropDownOption( "-- Custom Config ---"));
        foreach (var config in _customSpeakerConfigs)
        {
            _options.Add(new AudioDropDownOption(config));
        }
        
        _audioModeDropdown.options = _options;
    }

    private void Awake()
    {
        _audioModeDropdown.onValueChanged.AddListener(OnAudioModeChanged);
        UpdateDropdown();
        _audioModeDropdown.value = 0;
    }

    private void OnAudioModeChanged(int index)
    {
        if (index == -1) return;
        if (index == _lastModeIndex) return;
        
        var option = _audioModeDropdown.options[index] as AudioDropDownOption;
        if (option == null) return;
        if (option.onlyText)
        {
            _audioModeDropdown.SetValueWithoutNotify(_lastModeIndex);
            return;
        }

        if (option.onlyText) return;
        
        Debug.Log("Changing audio mode to " + option.mode);
        
        _ndiSender.audioMode = option.mode;
        if (option.mode == NdiSender.AudioMode.CustomSpeakerConfig)
        {
            _ndiSender.customSpeakerConfig = option.config;
        }
    }


}
