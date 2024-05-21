using System;
using UnityEngine;

namespace Klak.Ndi.Audio
{
    [RequireComponent(typeof(AudioSource))]
    public class AudioSourceListener : MonoBehaviour
    {

        [Serializable]
        public struct AdditionalSettings
        {
            public bool forceToChannel;
            public int channel;
        }
        
        public AdditionalSettings additionalSettings;
        
        private AudioSource _audioSource;
        private VirtualAudio.AudioSourceData _audioSourceData;
        
        private VirtualAudio.AudioSourceSettings _TmpSettings;

        private object _lockObj = new object();
        
        private void OnAudioFilterRead(float[] data, int channels)
        {

            lock (_lockObj)
            {
                if (_audioSourceData == null)
                {
                    return;
                }
            }
            
            int samples =  data.Length / channels;
            if (_audioSourceData.data == null || _audioSourceData.data.Length != samples)
                _audioSourceData.data = new float[samples];

            int sampleIndex = 0;
            for (int i = 0; i < data.Length; i += channels)
            {
                // Mix all channels into one
                
                float v = 0;
                for (int c = 0; c < channels; c++)
                {
                    v = VirtualAudio.MixSample(v, data[i + c]);
                }

                _audioSourceData.data[sampleIndex] = v;
                
                sampleIndex++;
            }
            
            lock (_lockObj)
            {
                _audioSourceData.settings = _TmpSettings;
            }
            
        }

        private void Update()
        {
            _TmpSettings.position = transform.position;
            _TmpSettings.spatialBlend = _audioSource.spatialBlend;
            _TmpSettings.volume = _audioSource.volume;
        }

        private void Awake()
        {
            _audioSource = GetComponent<AudioSource>();
            if (!_audioSource)
            {
                Debug.LogError("AudioSourceListener requires an AudioSource component.");
                enabled = false;
            }
            
            _TmpSettings.minDistance = _audioSource.minDistance;
            _TmpSettings.maxDistance = _audioSource.maxDistance;
            _TmpSettings.rolloffMode = _audioSource.rolloffMode;
            _TmpSettings.customRolloffCurve = _audioSource.GetCustomCurve(AudioSourceCurveType.CustomRolloff);
            _TmpSettings.spatialBlendCurve =  _audioSource.GetCustomCurve(AudioSourceCurveType.SpatialBlend);
        }

        private void OnEnable()
        {
            // We need raw audio data without any spatialization
            _audioSource.spatialize = VirtualAudio.useVirtualAudio;
            _audioSource.spatializePostEffects = VirtualAudio.useVirtualAudio;

            if (!VirtualAudio.useVirtualAudio)
                return;
            
            lock (_lockObj)
            {
                _audioSourceData = VirtualAudio.RegisterAudioSourceChannel( additionalSettings.forceToChannel ? additionalSettings.channel : -1);
            }
        }
        
        private void OnDisable()
        {
            lock (_lockObj)
            {
                if (_audioSourceData == null)
                    return;
                
                VirtualAudio.UnRegisterAudioSource(_audioSourceData);
                _audioSourceData = null;
            }
        }
        
    }
}