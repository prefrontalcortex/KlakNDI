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

    private List<float> spectrum = new List<float>(4000);
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
        {
            spectrum.AddRange(data);
            _maxVolume = m;
        }
    }
    
    public Gradient gradient;

    // Update is called once per frame
    void Update()
    {
        float vol;
        lock (_lockObj)
        {
            vol = _maxVolume;
        var e = new ParticleSystem.EmitParams();

        
        for (int i = 0; i < spectrum.Count; i++)
        {

            vol = spectrum[i];
            e.startColor = gradient.Evaluate(vol);
            //e.startColor = Color.Lerp(Color.green, Color.red, Mathf.Clamp01(vol * 1f));
            e.startLifetime = vol * 1.2f;
            e.startSize = vol * 0.5f;
            _particleSystem.Emit(e, 1);//Mathf.RoundToInt(vol * 3f));
        }
        spectrum.Clear();
        }
    }
}
