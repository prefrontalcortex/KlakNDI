using Klak.Ndi;
using Klak.Ndi.Audio;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class VirtualAudioSettings : MonoBehaviour
{
    [SerializeField] private NdiSender _ndiSender;
    [Header("UI References")]
    [SerializeField] private CanvasGroup _activeCanvasGroup;
    [SerializeField] private Toggle _playerCenteredAudioOnAllSpeakersToggle;
    [SerializeField] private Toggle _attenuationFromCameraPositionToggle;
    [SerializeField] private Slider _maxObjectsBasedAudioSlider;
    [SerializeField] private TextMeshProUGUI _objectBasedChannelCountText;
    [SerializeField] private Slider _defaultSpeakerDistances;
    [SerializeField] private TextMeshProUGUI _defaultSpeakerDistaceText;
    
    [SerializeField] private GameObject _virtualAudioActive;
    
    private void Awake()
    {
        _playerCenteredAudioOnAllSpeakersToggle.onValueChanged.AddListener(OnPlayerCenteredAudioOnAllSpeakersToggle);
        _maxObjectsBasedAudioSlider.onValueChanged.AddListener(OnMaxObjectsBasedAudioSlider);
        _attenuationFromCameraPositionToggle.onValueChanged.AddListener(OnAttenuationFromCameraPositionToggle);
        _defaultSpeakerDistances.onValueChanged.AddListener(OnDefaultSpeakerDistances);
        _virtualAudioActive.SetActive(VirtualAudio.UseVirtualAudio);
        FixedUpdate();
    }

    private void OnDefaultSpeakerDistances(float distance)
    {
        if (_ndiSender)
        {
            _ndiSender.virtualListenerDistance = distance;
        }
        UpdateTexts();
    }

    private void OnAttenuationFromCameraPositionToggle(bool isOn)
    {
        _ndiSender.useAudioOriginPositionForVirtualAttenuation = isOn;
    }

    private void UpdateTexts()
    {
        _objectBasedChannelCountText.text = VirtualAudio.MaxObjectBasedChannels.ToString();
        if (_ndiSender)
            _defaultSpeakerDistaceText.text = _ndiSender.virtualListenerDistance.ToString("F1")+ "m";
        else
            _defaultSpeakerDistaceText.text = "";
    }

    private void OnMaxObjectsBasedAudioSlider(float newCount)
    {
        if (_ndiSender)
            _ndiSender.maxObjectBasedChannels = (int)newCount;
        VirtualAudio.SetMaxObjectBasedChannels((int)newCount);
        UpdateTexts();
    }

    private void OnPlayerCenteredAudioOnAllSpeakersToggle(bool isOn)
    {
        if (_ndiSender)
            _ndiSender.playCenteredAudioSourcesOnAllSpeakers = isOn;
        
        VirtualAudio.PlayCenteredAudioSourceOnAllListeners = isOn; 
    }

    private void FixedUpdate()
    {
        if (VirtualAudio.PlayCenteredAudioSourceOnAllListeners != _playerCenteredAudioOnAllSpeakersToggle.isOn)
        {
            _playerCenteredAudioOnAllSpeakersToggle.SetIsOnWithoutNotify(VirtualAudio.PlayCenteredAudioSourceOnAllListeners);
            if (_ndiSender)
                _ndiSender.playCenteredAudioSourcesOnAllSpeakers = VirtualAudio.PlayCenteredAudioSourceOnAllListeners;

        }
        
        if (_ndiSender && _defaultSpeakerDistances.value != _ndiSender.virtualListenerDistance)
        {
            _defaultSpeakerDistances.SetValueWithoutNotify(_ndiSender.virtualListenerDistance);
            UpdateTexts();
        }
        
        if (VirtualAudio.MaxObjectBasedChannels != (int)_maxObjectsBasedAudioSlider.value)
        {
            if (_ndiSender)
                _ndiSender.maxObjectBasedChannels = (int)VirtualAudio.MaxObjectBasedChannels;
            _maxObjectsBasedAudioSlider.SetValueWithoutNotify(VirtualAudio.MaxObjectBasedChannels);
            UpdateTexts();
        }

        if (_ndiSender && _ndiSender.useAudioOriginPositionForVirtualAttenuation !=
            _attenuationFromCameraPositionToggle.isOn)
        {
            _attenuationFromCameraPositionToggle.SetIsOnWithoutNotify(_ndiSender.useAudioOriginPositionForVirtualAttenuation);
        }

        if (VirtualAudio.ObjectBasedAudio != _maxObjectsBasedAudioSlider.interactable)
        {
            _maxObjectsBasedAudioSlider.interactable = VirtualAudio.ObjectBasedAudio;
            _attenuationFromCameraPositionToggle.interactable = !VirtualAudio.ObjectBasedAudio;
            _playerCenteredAudioOnAllSpeakersToggle.interactable = !VirtualAudio.ObjectBasedAudio;
        }

        if (_ndiSender)
        {
            var usesDefaultSpeakerSetup = _ndiSender.audioMode == NdiSender.AudioMode.Virtual5Point1
                                          || _ndiSender.audioMode == NdiSender.AudioMode.Virtual7Point1 ||
                                          _ndiSender.audioMode == NdiSender.AudioMode.Virtual32Array
                                          || _ndiSender.audioMode == NdiSender.AudioMode.VirtualQuad;
            if (usesDefaultSpeakerSetup != _defaultSpeakerDistances.interactable)
                _defaultSpeakerDistances.interactable = usesDefaultSpeakerSetup;
        }

        if (VirtualAudio.UseVirtualAudio != _activeCanvasGroup.interactable)
        {
            _virtualAudioActive.SetActive(VirtualAudio.UseVirtualAudio);
            _activeCanvasGroup.interactable = VirtualAudio.UseVirtualAudio;
        }
            
    }
}
