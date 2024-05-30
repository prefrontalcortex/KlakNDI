using System;
using System.Collections;
using System.Collections.Generic;
using Klak.Ndi.Audio;
using UnityEngine;

public class ParticleController : MonoBehaviour
{
    [SerializeField] private ParticleSystem _particleSystem;
    
    // Start is called before the first frame update
    void Start()
    {
        if (_particleSystem == null)
            _particleSystem = GetComponentInChildren<ParticleSystem>();
    }

    private object _lockObj = new object();
    private float _maxVolume = 0;

    private void OnAudioFilterRead(float[] data, int channels)
    {
        float m = 0;
        for (int i = 0; i < data.Length; i++)
        {
            var d = Mathf.Abs(data[i]);
            if (d > m)
                m = d;
        }

        lock (_lockObj)
            _maxVolume = m;

    }

    // Update is called once per frame
    void Update()
    {
        lock (_lockObj)
        {
            _particleSystem.Emit( Mathf.RoundToInt(_maxVolume * 50f));
        }
        
    }
}
