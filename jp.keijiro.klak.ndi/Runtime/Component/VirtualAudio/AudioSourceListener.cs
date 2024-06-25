using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
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

            lock (_lockObj)
            {
                listenerWeights = _audioSourceData.currentWeights;
                _audioSourceData.settings = _TmpSettings;
                
                VirtualAudio.SetAudioDataFromSource(_audioSourceData.id, data, channels);
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
        }

        private void VirtualAudioStateChanged(bool active)
        {
            _audioSource.spatialize = VirtualAudio.UseVirtualAudio;
            _audioSource.spatializePostEffects = VirtualAudio.UseVirtualAudio;

            if (active)
            {
                lock (_lockObj)
                {
                    _audioSourceData = VirtualAudio.RegisterAudioSourceChannel();
                }
            }
            else
            {
                lock (_lockObj)
                {
                    if (_audioSourceData != null)
                    {
                        VirtualAudio.UnregisterAudioSource(_audioSourceData);
                        _audioSourceData = null;
                    }
                }
            }
        }
        
        private void OnEnable()
        {
            // We need raw audio data without any spatialization
            _audioSource.spatialize = VirtualAudio.UseVirtualAudio;
            _audioSource.spatializePostEffects = VirtualAudio.UseVirtualAudio;
            
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
            
            VirtualAudio.OnVirtualAudioStateChanged.AddListener(VirtualAudioStateChanged);

            if (!VirtualAudio.UseVirtualAudio)
                return;
            
            lock (_lockObj)
            {
                _audioSourceData = VirtualAudio.RegisterAudioSourceChannel();
            }
        }
        
        private void OnDisable()
        {
            VirtualAudio.OnVirtualAudioStateChanged.RemoveListener(VirtualAudioStateChanged);
            lock (_lockObj)
            {
                if (_audioSourceData == null)
                    return;
                
                VirtualAudio.UnregisterAudioSource(_audioSourceData);
                _audioSourceData = null;
            }
        }
        
    }
}