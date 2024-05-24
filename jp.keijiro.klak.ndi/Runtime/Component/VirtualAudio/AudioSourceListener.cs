using System;
using UnityEngine;

namespace Klak.Ndi.Audio
{
    [RequireComponent(typeof(AudioSource))]
    public class AudioSourceListener : MonoBehaviour
    {
        // TODO: support multiple channel audio files

        [Serializable]
        public struct AdditionalSettings
        {
            public bool forceToChannel;
            public int channel;
        }
        
        public AdditionalSettings additionalSettings;
        
        [SerializeField] private bool _showDebugGizmos = false;
        
        private AudioSource _audioSource;
        private VirtualAudio.AudioSourceData _audioSourceData;
        
        private VirtualAudio.AudioSourceSettings _TmpSettings;

        private object _lockObj = new object();

        private float[] listenerWeights;
        
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
                int nonNullChannels = 0;
                for (int c = 0; c < channels; c++)
                {
                    v += data[i + c];
                    if (data[i + c] != 0)
                        nonNullChannels++;
                }

                if (nonNullChannels == 0)
                    v = 0f;
                else
                    v /= nonNullChannels;

                for (int c = 0; c < channels; c++)
                    data[i+c] = v;
                
                _audioSourceData.data[sampleIndex] = v;
                
                sampleIndex++;
            }
            
            lock (_lockObj)
            {
                listenerWeights = _audioSourceData.lastListenerWeights;
                _audioSourceData.settings = _TmpSettings;
            }
        }

        private void Update()
        {
            _TmpSettings.position = transform.position;
            _TmpSettings.spatialBlend = _audioSource.spatialBlend;
            _TmpSettings.volume = _audioSource.volume;
            _TmpSettings.forceToChannel = additionalSettings.forceToChannel ? additionalSettings.channel : -1;
        }

        private void OnDrawGizmosSelected()
        {
            if (!_showDebugGizmos || !Application.isPlaying)
                return;

            float[] tmpListenerWeights = null;
            lock (_lockObj)
            {
                tmpListenerWeights = listenerWeights;
            }

            if (tmpListenerWeights == null)
                return;

            if (!Camera.main)
                return;
            
            var listenersPositions = VirtualAudio.GetListenersPositions();
            if (listenersPositions.Length != tmpListenerWeights.Length)
                return;

            var audioListener = GameObject.FindObjectOfType<AudioListener>();
            if (!audioListener)
                return;
            
            var listenerPos = audioListener.transform.position;

            for (int i = 0; i < listenersPositions.Length; i++)
            {
                Gizmos.color = tmpListenerWeights[i] > 0 ? new Color(0, 1, 0, 0.5f) : new Color(1, 0, 0, 0.5f);
                listenersPositions[i] = listenersPositions[i] + listenerPos;
                Gizmos.DrawWireSphere( listenersPositions[i], 1f);
            }
            
            for (int i = 0; i < tmpListenerWeights.Length; i++)
            {
                if (tmpListenerWeights[i] > 0)
                {
                    Gizmos.color = Color.black;
                    Gizmos.DrawLine( listenersPositions[i], transform.position);
                    Gizmos.color = Color.green;
                    var dir =  listenersPositions[i] - transform.position;
                    Gizmos.DrawLine( transform.position, transform.position + dir * listenerWeights[i]);
                    
                }
            }
        }

        private void Awake()
        {
            _audioSource = GetComponent<AudioSource>();
            if (!_audioSource)
            {
                Debug.LogError("AudioSourceListener requires an AudioSource component.");
                enabled = false;
            }

            _TmpSettings.position = transform.position;
            _TmpSettings.spatialBlend = _audioSource.spatialBlend;
            _TmpSettings.volume = _audioSource.volume;
            _TmpSettings.forceToChannel = additionalSettings.forceToChannel ? additionalSettings.channel : -1;
            
            _audioSource.bypassListenerEffects = false;
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
                _audioSourceData = VirtualAudio.RegisterAudioSourceChannel();
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