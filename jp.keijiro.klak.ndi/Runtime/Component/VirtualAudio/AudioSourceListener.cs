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
            
            int samples =  data.Length / channels;
            _audioSourceData.CheckDataSize(samples);

            unsafe
            {
                var dataPtr = UnsafeUtility.PinGCArrayAndGetDataAddress(data, out var handle);
                var nativeData = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<float>(dataPtr, data.Length, Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                var safety = AtomicSafetyHandle.Create();
                NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref nativeData, safety);
#endif
                lock (_lockObj)
                {
                    
                    _audioSourceData.CheckDataSize(samples);
                    var inputPtr = (float*)dataPtr;
                    var outputPtr = (float*)_audioSourceData.audioData.GetUnsafePtr();
                    
                    VirtualAudio.BurstMethods.MixToMono(inputPtr, data.Length, outputPtr, channels);
                    listenerWeights = _audioSourceData.lastListenerWeights;
                    _audioSourceData.settings = _TmpSettings;
                }
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.Release(safety);
#endif
                UnsafeUtility.ReleaseGCObject(handle);
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
                        VirtualAudio.UnRegisterAudioSource(_audioSourceData);
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
                
                VirtualAudio.UnRegisterAudioSource(_audioSourceData);
                _audioSourceData = null;
            }
        }
        
    }
}