using System.Collections.Generic;
using System.Linq;
using Klak.Ndi;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class AudioChannelsVisualisation : MonoBehaviour
{
    [SerializeField] private NdiReceiver _ndiReceiver;
    [SerializeField] private GameObject _channelTemplate;
    [SerializeField] private Color _activeColor = Color.green;
    [SerializeField] private Color _inactiveColor = Color.red;
    
    private List<GameObject> _audioChannels = new List<GameObject>();
    private List<Image> _audioChannelsImages = new List<Image>();
    private List<Image> _audioChannelsActiveImages = new List<Image>();
    private int _activeChannels = 0;

    private void UpdateLevelObjects(int requiredChannels)
    {
        if (requiredChannels > _audioChannelsImages.Count)
        {
            while (requiredChannels > _audioChannelsImages.Count)
            {
                var newChannel = Instantiate(_channelTemplate, _channelTemplate.transform.parent);
                newChannel.SetActive(true);
                _audioChannels.Add(newChannel);
                var images = newChannel.GetComponentsInChildren<Image>();
                var levelImage = images.FirstOrDefault(c => c.gameObject.name == "Level");;
                var activeImage = images.FirstOrDefault(c => c.gameObject.name == "Active");;

                _audioChannelsImages.Add(levelImage);
                _audioChannelsActiveImages.Add(activeImage);
                var channelText = newChannel.GetComponentInChildren<TextMeshProUGUI>();
                channelText.text = _audioChannels.Count.ToString();
            }
        }

        if (_activeChannels > requiredChannels)
        {
            for (int i = 0; i < _audioChannels.Count; i++)
            {
                _audioChannels[i].SetActive(i < requiredChannels);
            }

            _activeChannels = requiredChannels;
        }
    }

    private void Awake()
    {
        _channelTemplate.SetActive(false);
    }

    // Update is called once per frame
    void Update()
    {
        var channels = _ndiReceiver.GetChannelVisualisations();
        if (channels == null)
        {
            UpdateLevelObjects(0);
            return;
        }
        UpdateLevelObjects(channels.Length);
        
        for (int i = 0; i < channels.Length; i++)
        {
            _audioChannelsActiveImages[i].color = channels[i] > 0f ? _activeColor : _inactiveColor;
            _audioChannelsImages[i].fillAmount = channels[i];
        }
    }
}
