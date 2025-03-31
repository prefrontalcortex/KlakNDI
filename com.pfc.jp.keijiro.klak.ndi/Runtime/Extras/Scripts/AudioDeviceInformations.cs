using System.Collections.Generic;
using Klak.Ndi.Audio;
using TMPro;
using UnityEngine;

public class AudioDeviceInformations : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI _sampleRate;
    [SerializeField] private TMP_Dropdown _speakerModeSelection;
    [SerializeField] private TextMeshProUGUI _driverCapabilities;
    
    private void UpdateAudioSettings()
    {
        var config = AudioSettings.GetConfiguration();
        _sampleRate.text = config.sampleRate.ToString();
        _driverCapabilities.text = AudioSettings.driverCapabilities.ToString();
        
        _speakerModeSelection.ClearOptions();
        var capabilities = AudioChannelCount(AudioSettings.driverCapabilities);
        var options = new List<TMP_Dropdown.OptionData>();

        int currentSpeakerModeSelection = -1;
        void AddOption(AudioSpeakerMode mode)
        {
            var chCnt = AudioChannelCount(mode);
            if (chCnt <= capabilities)
                options.Add(new TMP_Dropdown.OptionData(mode.ToString()));
            if (mode == config.speakerMode)
                currentSpeakerModeSelection = options.Count - 1;
        }
        options.Add(new TMP_Dropdown.OptionData("Mute"));

        AddOption(AudioSpeakerMode.Mono);
        AddOption(AudioSpeakerMode.Stereo);
        AddOption(AudioSpeakerMode.Quad);
        AddOption(AudioSpeakerMode.Surround);
        AddOption(AudioSpeakerMode.Mode5point1);
        AddOption(AudioSpeakerMode.Mode7point1);
        
        _speakerModeSelection.AddOptions(options);
        _speakerModeSelection.SetValueWithoutNotify(currentSpeakerModeSelection);
    }
    
    private static int AudioChannelCount(AudioSpeakerMode speakerMode)
    {
        switch (speakerMode)
        {
            case AudioSpeakerMode.Mono: return 1;
            case AudioSpeakerMode.Stereo: return 2;
            case AudioSpeakerMode.Quad: return 4;
            case AudioSpeakerMode.Surround: return 5;
            case AudioSpeakerMode.Mode5point1: return 6;
            case AudioSpeakerMode.Mode7point1: return 8;
            default:
                return 2;
        }
    }

    private void Awake()
    {
        _speakerModeSelection.onValueChanged.AddListener(index =>
        {
            if (index == 0)
            {
                VirtualAudio.MuteAudioOutput = true;
                return;
            }
            VirtualAudio.MuteAudioOutput = false;
            var config = AudioSettings.GetConfiguration();
            if (AudioSpeakerMode.TryParse(_speakerModeSelection.options[index].text, out AudioSpeakerMode mode))
            {
                config.speakerMode = mode;
                AudioSettings.Reset(config);
                UpdateAudioSettings();
            }
        });
        
        AudioSettings.OnAudioConfigurationChanged += AudioSettingsOnOnAudioConfigurationChanged;
        UpdateAudioSettings();
    }

    private void AudioSettingsOnOnAudioConfigurationChanged(bool devicewaschanged)
    {
        UpdateAudioSettings();
    }
    
}
