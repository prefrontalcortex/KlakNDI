using System.Collections.Generic;
using Klak.Ndi;
using UnityEngine;

public class SpeakersVisualisation : MonoBehaviour
{
    [SerializeField] private NdiReceiver _ndiReceiver;
    [SerializeField] private GameObject _speakerTemplate;
    
    private List<Transform> _speakers = new List<Transform>();
    
    private void UpdateSpeakers()
    {
        var speakerPositions = _ndiReceiver.GetCurrentSpeakerPositions();
        if (speakerPositions == null)
        {
            for (int i = 0; i < _speakers.Count; i++)
                _speakers[i].gameObject.SetActive(false);  
            return;
        }

        if (speakerPositions.Length > _speakers.Count)
        {
            while (speakerPositions.Length > _speakers.Count)
            {
                var newSpeaker = Instantiate(_speakerTemplate, _speakerTemplate.transform.parent);
                newSpeaker.SetActive(true);
                _speakers.Add(newSpeaker.transform);
                var speakerNoGO = newSpeaker.transform.Find("speakerNo");
                if (speakerNoGO)
                {
                    var text = speakerNoGO.GetComponent<TMPro.TextMeshPro>();
                    text.text = _speakers.Count.ToString();
                }
            }
        }


        for (int i = 0; i < _speakers.Count; i++)
            _speakers[i].gameObject.SetActive(i < speakerPositions.Length);
        
        for (int i = 0; i < speakerPositions.Length; i++)
            _speakers[i].SetPositionAndRotation(speakerPositions[i], speakerPositions[i] == Vector3.zero ? Quaternion.identity : Quaternion.LookRotation(-speakerPositions[i].normalized));

        var speakerLevel = _ndiReceiver.GetChannelVisualisations();
        if (speakerLevel == null)
        {
            for (int i = 0; i < _speakers.Count; i++)
                _speakers[i].localScale = Vector3.Lerp(_speakers[i].localScale, Vector3.one, Time.deltaTime * 4f);
            return;
        }

        if (speakerLevel.Length > _speakers.Count)
            return;
        
        for (int i = 0; i < speakerLevel.Length; i++)
        {
            var newScale = Vector3.one + Vector3.one * Mathf.Min(2f, speakerLevel[i] * 10f);
            if (newScale.magnitude < _speakers[i].localScale.magnitude)
                _speakers[i].localScale = Vector3.Lerp(_speakers[i].localScale, newScale, Time.deltaTime * 4f);
            else
                _speakers[i].localScale = newScale;

        }
    }
    
    private void Awake()
    {
        _speakerTemplate.SetActive(false);
    }
    
    private void Update()
    {
        UpdateSpeakers();
    }
}
