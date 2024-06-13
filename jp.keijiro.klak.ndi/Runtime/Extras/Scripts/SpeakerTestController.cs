using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Klak.Ndi;
using Klak.Ndi.Audio;
using UnityEngine;
using UnityEngine.Serialization;

public class SpeakerTestController : MonoBehaviour
{
    [FormerlySerializedAs("_NdiSender")] [SerializeField] private NdiSender _ndiSender;
    
    [Header("Default Speaker Setup")]
    public GameObject mono;
    public GameObject speakersStereo;
    public GameObject speakersQuad;
    public GameObject speakers5_1;
    public GameObject speakers7_1;
    public GameObject speakersObjectBased;
    public SpeakerTest objectBasedChannelTemplate;
    public GameObject customSpeakerConfig;
    public SpeakerTest customSpeakerConfigTemplate;
    
    private List<SpeakerTest> _currentTestSpeakers = new List<SpeakerTest>();
    private GameObject _currentTest;
    private bool _currentIsGenerated = false;
    private NdiSender.AudioMode _lastAudioMode;

    private void Awake()
    {
        mono.SetActive(false);
        speakersStereo.SetActive(false);
        speakersQuad.SetActive(false);
        speakers5_1.SetActive(false);
        speakers7_1.SetActive(false);
        speakersObjectBased.SetActive(false);
        customSpeakerConfig.SetActive(false);
        objectBasedChannelTemplate.gameObject.SetActive(false);
        customSpeakerConfigTemplate.gameObject.SetActive(false);
    }

    public void SetAutoSwitch(bool active)
    {
        if (active)
            StartCoroutine(AutoChannelSwitch());
        else
            StopAllCoroutines();
    }

    IEnumerator AutoChannelSwitch()
    {
        int index = 0;
        do
        {
            if (index > _currentTestSpeakers.Count - 1)
                index = 0;

            PlaySpeaker(_currentTestSpeakers[index]);
            yield return new WaitForSeconds(2f);
            index++;

        } while (true);
    }
    
    private void OnEnable()
    {
        ClearLastTest();
        VirtualAudio.ActivateAudioTestMode(true);
        ActivateBySenderAudioMode(_ndiSender);
    }

    private void Update()
    {
        if (_ndiSender.audioMode != _lastAudioMode)
            ActivateBySenderAudioMode(_ndiSender);
        else
        {
            if ((_ndiSender.audioMode == NdiSender.AudioMode.CustomSpeakerConfig 
                || _ndiSender.audioMode == NdiSender.AudioMode.Virtual32Array) 
                 && _currentTestSpeakers.Count != VirtualAudio.GetListenersPositions().Length)
                ActivateBySenderAudioMode(_ndiSender);
            
            if (_ndiSender.audioMode == NdiSender.AudioMode.ObjectBased && _currentTestSpeakers.Count != _ndiSender.maxObjectBasedChannels)
                ActivateBySenderAudioMode(_ndiSender);
        }
    }

    private void OnDisable()
    {
        VirtualAudio.ActivateAudioTestMode(false);
        ClearLastTest();
    }

    private void ClearLastTest()
    {
        if (_currentTest)
            _currentTest.SetActive(false);
        if (_currentIsGenerated)
        {
            for (int i = 0; i < _currentTestSpeakers.Count; i++)
            {
                Destroy(_currentTestSpeakers[i].gameObject);
            }
        }
        _currentTest = null;
        _currentIsGenerated = false;
        _currentTestSpeakers.Clear();
    }
    
    private void RegisterPlayEvents()
    {
        foreach (var speaker in _currentTestSpeakers)
        {
            speaker.onPlayPressed.RemoveAllListeners();
            speaker.onPlayPressed.AddListener(PlaySpeaker);
        }
        if (_currentTestSpeakers.Count > 0)
            PlaySpeaker(_currentTestSpeakers[0]);
    }

    private void PlaySpeaker(SpeakerTest speaker)
    {
        foreach (var sp in _currentTestSpeakers)
            sp.SetActivePlaying(false);
        speaker.SetActivePlaying(true);
        
        Debug.Log("Set test channel to "+speaker.channelNo);
        VirtualAudio.SetAudioTestChannel(speaker.channelNo);
    }

    private void ActivateTests(GameObject speakers)
    {
        ClearLastTest();
        _currentIsGenerated = false;
        _currentTest = speakers;
        speakers.SetActive(true);
        _currentTestSpeakers = speakers.GetComponentsInChildren<SpeakerTest>().OrderBy( x => x.channelNo ).ToList();
        RegisterPlayEvents();
    }

    private void GenerateTests(Vector3[] positions)
    {
        ClearLastTest();
        _currentTest = customSpeakerConfig;
        _currentIsGenerated = true;

        float maxX = positions.Max(x => Mathf.Abs(x.x));
        float maxZ = positions.Max(x => Mathf.Abs(x.z));

        var contentParent = customSpeakerConfigTemplate.transform.parent.GetComponent<RectTransform>();
        var xScale = (contentParent.rect.width - 50) / 2f;
        var zScale = (contentParent.rect.height - 50) / 2f;
        var scale = new Vector2(xScale / maxX, zScale / maxZ);
        
        for (int i = 0; i < positions.Length; i++)
        {
            var speaker = Instantiate(customSpeakerConfigTemplate, customSpeakerConfigTemplate.transform.parent);
            speaker.channelNo = i;
            var rectTransform = speaker.GetComponent<RectTransform>();
            var pos = new Vector2(positions[i].x, positions[i].z);
            // Scale to fit the screen
            pos *= scale;
            rectTransform.anchoredPosition = pos;
            _currentTestSpeakers.Add(speaker);
            speaker.gameObject.SetActive(true);
        }
        customSpeakerConfig.SetActive(true);
        
        RegisterPlayEvents();
    }

    private void GenerateObjectBasedTest(int count)
    {
        ClearLastTest();
        _currentTest = speakersObjectBased;
        _currentIsGenerated = true;
        
        int channelCount = _ndiSender.maxObjectBasedChannels;
        for (int i = 0; i < channelCount; i++)
        {
            var speaker = Instantiate(objectBasedChannelTemplate, objectBasedChannelTemplate.transform.parent);
            speaker.channelNo = i;
            _currentTestSpeakers.Add(speaker);
            speaker.gameObject.SetActive(true);
        }
        speakersObjectBased.SetActive(true);
        RegisterPlayEvents();
    }
    
    public void ActivateBySenderAudioMode(NdiSender sender)
    {
        _lastAudioMode = sender.audioMode;
        ClearLastTest();
        switch (sender.audioMode)
        {
            case NdiSender.AudioMode.AudioListener:
                switch (AudioSettings.speakerMode)
                {
                    case AudioSpeakerMode.Mono: ActivateMono(); break;
                    case AudioSpeakerMode.Stereo: ActivateStereo(); break;
                    case AudioSpeakerMode.Quad: ActivateQuad(); break;
                    case AudioSpeakerMode.Surround:
                    case AudioSpeakerMode.Mode5point1: Activate5_1(); break;
                    case AudioSpeakerMode.Mode7point1: Activate7_1(); break;
                }
                break;
            case NdiSender.AudioMode.VirtualQuad: ActivateQuad(); break;
            case NdiSender.AudioMode.Virtual5Point1: Activate5_1(); break;
            case NdiSender.AudioMode.Virtual7Point1: Activate7_1(); break;
            case NdiSender.AudioMode.Virtual32Array: 
            case NdiSender.AudioMode.CustomSpeakerConfig:
                GenerateTests(VirtualAudio.GetListenersPositions());
                break;
            case NdiSender.AudioMode.ObjectBased:
                GenerateObjectBasedTest(sender.maxObjectBasedChannels);
                break;
            default:
                GenerateTests(VirtualAudio.GetListenersPositions());
                break;
        }
    }
    
    public void ActivateMono()
    {
        ActivateTests(mono);
    }

    public void ActivateStereo()
    {
        ActivateTests(speakersStereo);
    }
    
    public void ActivateQuad()
    {
        ActivateTests(speakersQuad);
    }
    
    public void Activate5_1()
    {
        ActivateTests(speakers5_1);
    }

    public void Activate7_1()
    {
        ActivateTests(speakers7_1);
    }
    
}
