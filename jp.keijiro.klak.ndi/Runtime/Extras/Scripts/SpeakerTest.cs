using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class SpeakerTest : MonoBehaviour
{
    public int channelNo = 0;
    [Header("References")]
    [SerializeField] private GameObject _isActivated;
    [SerializeField] private TextMeshProUGUI _channelNoText;
    [SerializeField] private Button _playButton;
    [HideInInspector] public UnityEvent<SpeakerTest> onPlayPressed;

    public void OnEnable()
    {
        _isActivated.SetActive(false);
        _channelNoText.text = channelNo.ToString();
    }

    public void SetActivePlaying(bool isActive)
    {
        _isActivated.SetActive(isActive);
    }

    private void Awake()
    {
        _isActivated.SetActive(false);
        _playButton.onClick.AddListener(Play);
    }

    private void Play()
    {
        onPlayPressed.Invoke(this);
    }
}
